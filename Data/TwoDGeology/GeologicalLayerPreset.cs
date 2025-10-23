// GeoscientistToolkit/Business/GIS/GeologicalLayerPresets.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Provides complete geological cross-section presets with multiple layers
/// FINAL VERSION: Layers touch topography, crystalline basement always present, no bypassing
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
        
        // CRITICAL: Validate geology
        ValidateAndFixGeology(section);
        
        return section;
    }
    
    private static void ValidateAndFixGeology(CrossSection section)
    {
        if (section.Profile == null || section.Formations.Count == 0)
            return;
        
        // Ensure first layer touches topography
        EnsureTopLayerTouchesTopography(section);
        
        // Clip all formations to topography
        foreach (var formation in section.Formations)
        {
            ClipFormationToTopography(formation, section.Profile);
        }
        
        // Fix overlaps
        FixOverlappingFormations(section.Formations);
        
        // Ensure all layers respect basement
        EnsureAllLayersRespectBasement(section);
    }
    
    /// <summary>
    /// CRITICAL: Make sure the topmost sedimentary layer touches the topography exactly
    /// </summary>
    private static void EnsureTopLayerTouchesTopography(CrossSection section)
    {
        // Find the first non-basement formation (topmost sedimentary layer)
        var topFormation = section.Formations.FirstOrDefault(f => f.Name != "Crystalline Basement");
        
        if (topFormation == null || topFormation.TopBoundary.Count == 0)
            return;
        
        var profile = section.Profile;
        
        // Force top boundary to match topography exactly
        for (int i = 0; i < topFormation.TopBoundary.Count && i < profile.Points.Count; i++)
        {
            float x = topFormation.TopBoundary[i].X;
            float topoElevation = GetTopographyElevationAt(profile, x);
            
            topFormation.TopBoundary[i] = new Vector2(x, topoElevation);
        }
    }
    
    /// <summary>
    /// CRITICAL: Ensure no layer goes below crystalline basement
    /// </summary>
    private static void EnsureAllLayersRespectBasement(CrossSection section)
    {
        // Find the basement
        var basement = section.Formations.FirstOrDefault(f => f.Name == "Crystalline Basement");
        
        if (basement == null || basement.TopBoundary.Count == 0)
            return;
        
        // Clip all other formations to not go below basement top
        foreach (var formation in section.Formations)
        {
            if (formation.Name == "Crystalline Basement")
                continue;
            
            for (int i = 0; i < formation.BottomBoundary.Count && i < basement.TopBoundary.Count; i++)
            {
                float basementTop = basement.TopBoundary[i].Y;
                
                // Bottom boundary cannot go below basement
                if (formation.BottomBoundary[i].Y < basementTop)
                {
                    formation.BottomBoundary[i] = new Vector2(
                        formation.BottomBoundary[i].X,
                        basementTop
                    );
                }
                
                // If top also goes below basement, clip it too
                if (i < formation.TopBoundary.Count && formation.TopBoundary[i].Y < basementTop)
                {
                    formation.TopBoundary[i] = new Vector2(
                        formation.TopBoundary[i].X,
                        basementTop
                    );
                }
            }
        }
    }
    
    private static void ClipFormationToTopography(ProjectedFormation formation, GeologicalMapping.ProfileGenerator.TopographicProfile profile)
    {
        if (formation.TopBoundary.Count == 0 || formation.Name == "Crystalline Basement")
            return;
        
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            var point = formation.TopBoundary[i];
            float topoElevation = GetTopographyElevationAt(profile, point.X);
            
            if (point.Y > topoElevation)
            {
                formation.TopBoundary[i] = new Vector2(point.X, topoElevation);
            }
            
            if (i < formation.BottomBoundary.Count)
            {
                var bottomPoint = formation.BottomBoundary[i];
                if (bottomPoint.Y > formation.TopBoundary[i].Y)
                {
                    formation.BottomBoundary[i] = new Vector2(bottomPoint.X, formation.TopBoundary[i].Y);
                }
            }
        }
    }
    
    private static float GetTopographyElevationAt(GeologicalMapping.ProfileGenerator.TopographicProfile profile, float x)
    {
        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            if (x >= profile.Points[i].Distance && x <= profile.Points[i + 1].Distance)
            {
                float t = (x - profile.Points[i].Distance) / (profile.Points[i + 1].Distance - profile.Points[i].Distance);
                return profile.Points[i].Elevation + t * (profile.Points[i + 1].Elevation - profile.Points[i].Elevation);
            }
        }
        
        if (x < profile.Points[0].Distance)
            return profile.Points[0].Elevation;
        if (x > profile.Points[^1].Distance)
            return profile.Points[^1].Elevation;
        
        return profile.Points[0].Elevation;
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
                
                if (j < currentFormation.BottomBoundary.Count && 
                    currentFormation.BottomBoundary[j].Y > currentFormation.TopBoundary[j].Y)
                {
                    currentFormation.BottomBoundary[j] = new Vector2(
                        currentFormation.BottomBoundary[j].X,
                        currentFormation.TopBoundary[j].Y
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
            new LayerDefinition { Name = "Jurassic Limestone", Color = new Vector4(0.75f, 0.8f, 0.85f, 0.8f), Thickness = 400f },
            new LayerDefinition { Name = "Triassic Sandstone", Color = new Vector4(0.9f, 0.65f, 0.4f, 0.8f), Thickness = 300f },
            new LayerDefinition { Name = "Permian Shale", Color = new Vector4(0.5f, 0.4f, 0.4f, 0.8f), Thickness = 200f },
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
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 100f);
        var layers = GetStandardStratigraphicColumn();
        
        float regionalDip = 5f;
        var profile = section.Profile;
        
        // 🔥 FIRST: Add crystalline basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        // Then add sedimentary layers ABOVE basement
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
            
            for (int i = 0; i < profile.Points.Count; i++)
            {
                float x = profile.Points[i].Distance;
                float dipOffset = (x / totalDistance) * totalDistance * MathF.Tan(regionalDip * MathF.PI / 180f);
                float topElevation;
                
                if (layerIdx == 0)
                {
                    // 🔥 First layer: MUST start at topography
                    topElevation = profile.Points[i].Elevation;
                }
                else
                {
                    // Subsequent layers: start at previous layer's bottom
                    var prevFormation = section.Formations[layerIdx]; // +1 because basement is at index 0
                    topElevation = prevFormation.BottomBoundary[i].Y;
                }
                
                topElevation -= dipOffset * layerIdx * 0.05f;
                
                formation.TopBoundary.Add(new Vector2(x, topElevation));
                formation.BottomBoundary.Add(new Vector2(x, topElevation - layer.Thickness));
            }
            
            section.Formations.Add(formation);
        }
        
        return section;
    }
    
    private static CrossSection CreateErodedAnticline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float t = i / (float)(profile.Points.Count - 1);
            float x = t * totalDistance;
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float valleyDepth = 200f * (1f - centerDist * centerDist);
            profile.Points[i].Elevation = -valleyDepth;
            profile.Points[i].Position = new Vector2(x, -valleyDepth);
        }
        profile.MinElevation = -200f;
        profile.MaxElevation = 0f;
        
        // 🔥 Add crystalline basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Skip(1).Take(5).ToArray();
        
        float centerX = totalDistance / 2f;
        float foldWidth = totalDistance * 0.7f;
        float amplitude = 500f;
        
        float currentBase = -700f;
        
        foreach (var layer in layers)
        {
            var formation = CreateAnticlineLayer(layer.Name, layer.Color, totalDistance,
                currentBase, layer.Thickness, centerX, foldWidth, amplitude, profile);
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
            amplitude *= 0.9f;
        }
        
        // 🔥 Valley fill - MUST touch topography
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
            float topoElev = profile.Points[i].Elevation; // 🔥 Exactly at topography
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float fillDepth = 100f * (1f - centerDist * centerDist);
            
            valleyFill.TopBoundary.Add(new Vector2(x, topoElev));
            valleyFill.BottomBoundary.Add(new Vector2(x, topoElev - fillDepth));
        }
        
        section.Formations.Insert(1, valleyFill); // After basement
        
        var fault = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 70f,
            DipDirection = "West",
            Displacement = 250f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.35f, 50f),
                new Vector2(totalDistance * 0.32f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault);
        
        return section;
    }
    
    private static CrossSection CreateErodedSyncline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float t = i / (float)(profile.Points.Count - 1);
            float elevation = 50f * MathF.Sin(t * MathF.PI * 2f);
            profile.Points[i].Elevation = elevation;
            profile.Points[i].Position = new Vector2(profile.Points[i].Distance, elevation);
        }
        profile.MinElevation = -50f;
        profile.MaxElevation = 50f;
        
        // 🔥 Add basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Skip(1).Take(5).ToArray();
        
        float centerX = totalDistance / 2f;
        float foldWidth = totalDistance * 0.7f;
        float amplitude = 500f;
        
        float currentBase = -100f;
        
        foreach (var layer in layers)
        {
            var formation = CreateSynclineLayer(layer.Name, layer.Color, totalDistance,
                currentBase, layer.Thickness, centerX, foldWidth, amplitude, profile);
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        
        return section;
    }
    
    private static CrossSection CreateJuraMountains(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float t = i / (float)(profile.Points.Count - 1);
            float x = t * totalDistance;
            
            float hill1 = 80f * MathF.Sin(x / (totalDistance * 0.3f) * MathF.PI);
            float hill2 = 50f * MathF.Sin(x / (totalDistance * 0.15f) * MathF.PI);
            float elevation = hill1 + hill2 + 100f;
            
            profile.Points[i].Elevation = elevation;
            profile.Points[i].Position = new Vector2(x, elevation);
        }
        profile.MinElevation = 0f;
        profile.MaxElevation = 250f;
        
        // 🔥 Basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Skip(2).Take(4).ToArray();
        
        float center1 = totalDistance * 0.25f;
        float center2 = totalDistance * 0.5f;
        float center3 = totalDistance * 0.75f;
        float foldWidth = totalDistance * 0.2f;
        float amplitude = 300f;
        
        float currentBase = -300f;
        
        foreach (var layer in layers)
        {
            var formation = CreateJuraStyleFold(layer.Name, layer.Color, totalDistance,
                currentBase, layer.Thickness, center1, center2, center3, foldWidth, amplitude, profile);
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        
        // 🔥 Multiple thrust faults
        var thrust1 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 25f,
            DipDirection = "East",
            Displacement = 800f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.2f, 200f),
                new Vector2(totalDistance * 0.3f, baseElevation + 600f)
            }
        };
        section.Faults.Add(thrust1);
        
        var thrust2 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 30f,
            DipDirection = "East",
            Displacement = 1000f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.45f, 220f),
                new Vector2(totalDistance * 0.55f, baseElevation + 700f)
            }
        };
        section.Faults.Add(thrust2);
        
        var thrust3 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 28f,
            DipDirection = "East",
            Displacement = 900f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.7f, 210f),
                new Vector2(totalDistance * 0.78f, baseElevation + 650f)
            }
        };
        section.Faults.Add(thrust3);
        
        var backThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 35f,
            DipDirection = "West",
            Displacement = 500f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.6f, 180f),
                new Vector2(totalDistance * 0.53f, baseElevation + 550f)
            }
        };
        section.Faults.Add(backThrust);
        
        return section;
    }
    
    private static CrossSection CreateFaultedLayers(float totalDistance, float baseElevation)
    {
        var section = CreateSimpleLayers(totalDistance, baseElevation);
        
        float fault1X = totalDistance * 0.45f;
        var fault1 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 65f,
            DipDirection = "West",
            Displacement = 400f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(fault1X, section.Profile.MaxElevation + 100f),
                new Vector2(fault1X - 500f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault1);
        
        foreach (var formation in section.Formations)
        {
            if (formation.Name != "Crystalline Basement")
                OffsetFormationByFault(formation, fault1X, 400f, true);
        }
        
        float fault2X = totalDistance * 0.65f;
        var fault2 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 50f,
            DipDirection = "East",
            Displacement = 200f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(fault2X, section.Profile.MaxElevation + 80f),
                new Vector2(fault2X + 350f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault2);
        
        return section;
    }
    
    private static CrossSection CreateThrustFault(float totalDistance, float baseElevation)
    {
        var section = CreateSimpleLayers(totalDistance, baseElevation);
        
        float faultX = totalDistance * 0.65f;
        var mainThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 20f,
            DipDirection = "West",
            Displacement = 2500f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(faultX, section.Profile.MaxElevation + 50f),
                new Vector2(faultX - 2500f, baseElevation + 600f)
            }
        };
        section.Faults.Add(mainThrust);
        
        var shortcutThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 35f,
            DipDirection = "West",
            Displacement = 400f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.4f, 100f),
                new Vector2(totalDistance * 0.32f, baseElevation + 500f)
            }
        };
        section.Faults.Add(shortcutThrust);
        
        return section;
    }
    
    private static CrossSection CreateFoldedSequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 100f);
        
        // 🔥 Basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        float currentBase = -100f;
        
        foreach (var layer in layers)
        {
            var formation = CreateWavyLayer(layer.Name, layer.Color, totalDistance,
                currentBase, layer.Thickness, 3, 120f, section.Profile);
            
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        
        var reverseFault = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Reverse,
            Dip = 55f,
            DipDirection = "East",
            Displacement = 350f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.6f, 150f),
                new Vector2(totalDistance * 0.68f, baseElevation + 500f)
            }
        };
        section.Faults.Add(reverseFault);
        
        return section;
    }
    
    private static CrossSection CreateUnconformitySequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 50f);
        
        // 🔥 Basement first
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        // 🔥 Lower tilted sequence (older rocks) - 30 degree dip
        var lowerLayers = new[]
        {
            new LayerDefinition { Name = "Devonian Sandstone", Color = new Vector4(0.7f, 0.6f, 0.5f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Silurian Shale", Color = new Vector4(0.6f, 0.6f, 0.7f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Ordovician Limestone", Color = new Vector4(0.5f, 0.5f, 0.6f, 0.8f), Thickness = 300f }
        };
        
        float tiltAngle = 30f; // Steep dip for dramatic unconformity
        float currentTop = -500f;
        
        foreach (var layer in lowerLayers)
        {
            var formation = CreateTiltedLayer(layer.Name, layer.Color, totalDistance,
                currentTop, layer.Thickness, tiltAngle);
            section.Formations.Add(formation);
            currentTop -= layer.Thickness * 0.7f;
        }
        
        // 🔥 FAULT cutting lower sequence
        var preFault = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 70f,
            DipDirection = "East",
            Displacement = 400f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.35f, -300f),
                new Vector2(totalDistance * 0.42f, baseElevation + 300f)
            }
        };
        section.Faults.Add(preFault);
        
        // Apply fault offset to lower layers only
        for (int i = 1; i <= lowerLayers.Length; i++)
        {
            OffsetFormationByFault(section.Formations[i], totalDistance * 0.35f, 400f, false);
        }
        
        // 🔥 Upper horizontal sequence (above unconformity - younger rocks)
        var upperLayers = new[]
        {
            new LayerDefinition { Name = "Permian Conglomerate", Color = new Vector4(0.8f, 0.7f, 0.6f, 0.8f), Thickness = 120f },
            new LayerDefinition { Name = "Triassic Sandstone", Color = new Vector4(0.9f, 0.65f, 0.4f, 0.8f), Thickness = 180f },
            new LayerDefinition { Name = "Jurassic Limestone", Color = new Vector4(0.75f, 0.8f, 0.85f, 0.8f), Thickness = 150f }
        };
        
        // Create unconformity surface - this is where erosion truncated the tilted layers
        currentTop = -250f;
        foreach (var layer in upperLayers)
        {
            var formation = CreateFlatLayer(layer.Name, layer.Color, totalDistance, currentTop, layer.Thickness);
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        return section;
    }
    
    private static CrossSection CreateBasinFilling(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            profile.Points[i].Elevation = 0f;
            profile.Points[i].Position = new Vector2(profile.Points[i].Distance, 0f);
        }
        
        // 🔥 Basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);
        
        float centerX = totalDistance / 2f;
        float basinWidth = totalDistance * 0.8f;
        
        var basinLayers = new[]
        {
            new LayerDefinition { Name = "Deep Marine Shale", Color = new Vector4(0.4f, 0.4f, 0.5f, 0.8f), Thickness = 400f },
            new LayerDefinition { Name = "Turbidites", Color = new Vector4(0.6f, 0.6f, 0.65f, 0.8f), Thickness = 300f },
            new LayerDefinition { Name = "Slope Sediments", Color = new Vector4(0.7f, 0.7f, 0.6f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Shallow Marine Sand", Color = new Vector4(0.85f, 0.8f, 0.6f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Deltaic Deposits", Color = new Vector4(0.8f, 0.75f, 0.55f, 0.8f), Thickness = 150f }
        };
        
        float currentTop = -800f;
        float currentBasinDepth = 800f;
        
        foreach (var layer in basinLayers)
        {
            var formation = CreateBasinLayer(layer.Name, layer.Color, totalDistance, centerX, basinWidth,
                currentTop, layer.Thickness, currentBasinDepth);
            section.Formations.Add(formation);
            currentTop += layer.Thickness * 0.7f;
            currentBasinDepth -= layer.Thickness * 0.5f;
        }
        
        var basinFault = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 60f,
            DipDirection = "East",
            Displacement = 600f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.8f, 50f),
                new Vector2(totalDistance * 0.85f, baseElevation + 400f)
            }
        };
        section.Faults.Add(basinFault);
        
        return section;
    }
    
    private static CrossSection CreateChannelFill(float totalDistance, float baseElevation)
    {
        var section = CreateSimpleLayers(totalDistance, baseElevation);
        
        float channelCenter = totalDistance * 0.5f;
        float channelWidth = 1500f;
        float incisionDepth = 100f;
        
        UpdateTopographyWithChannel(section.Profile, totalDistance, channelCenter, channelWidth, incisionDepth);
        
        var channelFill = CreateChannelFillFormation("Quaternary Channel Fill",
            new Vector4(0.85f, 0.8f, 0.6f, 0.8f),
            totalDistance, channelCenter, channelWidth, incisionDepth, section.Profile);
        
        section.Formations.Insert(1, channelFill); // After basement
        
        return section;
    }
    
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
            float elevation = topElevation + 50f * MathF.Sin(distance / 2000f * MathF.PI);
            
            profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, elevation),
                Distance = distance,
                Elevation = elevation,
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
    
    /// <summary>
    /// 🔥 CRITICAL: Create crystalline basement that all layers sit on
    /// </summary>
    private static ProjectedFormation CreateCrystallineBasement(float totalDistance, float baseElevation, float regionalDip)
    {
        var basement = new ProjectedFormation
        {
            Name = "Crystalline Basement",
            Color = new Vector4(0.4f, 0.4f, 0.5f, 0.9f), // Dark gray/blue
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 50;
        float dipRad = regionalDip * MathF.PI / 180f;
        
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            
            // Basement top can have gentle dip or be flat
            float basementTop = -1200f - (x / totalDistance * totalDistance * MathF.Tan(dipRad));
            float basementBottom = baseElevation; // Always at the very bottom
            
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
    
    private static ProjectedFormation CreateAnticlineLayer(string name, Vector4 color, float totalDistance,
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
            float foldFactor = Math.Max(0, 1 - (distFromCenter / (foldWidth / 2f)));
            foldFactor = foldFactor * foldFactor;
            
            float foldOffset = amplitude * foldFactor;
            float topElev = baseTop + foldOffset;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateSynclineLayer(string name, Vector4 color, float totalDistance,
        float baseTop, float thickness, float centerX, float foldWidth, float amplitude,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>(),
            FoldStyle = FoldStyle.Syncline
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float distFromCenter = Math.Abs(x - centerX);
            float foldFactor = Math.Max(0, 1 - (distFromCenter / (foldWidth / 2f)));
            foldFactor = foldFactor * foldFactor;
            
            float foldOffset = -amplitude * foldFactor;
            float topElev = baseTop + foldOffset;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateJuraStyleFold(string name, Vector4 color, float totalDistance,
        float baseTop, float thickness, float center1, float center2, float center3, float foldWidth, float amplitude,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile)
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
            
            float distFromCenter1 = Math.Abs(x - center1);
            float foldFactor1 = Math.Max(0, 1 - (distFromCenter1 / (foldWidth / 2f)));
            foldFactor1 = foldFactor1 * foldFactor1;
            
            float distFromCenter2 = Math.Abs(x - center2);
            float foldFactor2 = Math.Max(0, 1 - (distFromCenter2 / (foldWidth / 2f)));
            foldFactor2 = foldFactor2 * foldFactor2;
            
            float distFromCenter3 = Math.Abs(x - center3);
            float foldFactor3 = Math.Max(0, 1 - (distFromCenter3 / (foldWidth / 2f)));
            foldFactor3 = foldFactor3 * foldFactor3;
            
            float foldOffset = amplitude * (foldFactor1 + foldFactor2 + foldFactor3);
            float topElev = baseTop + foldOffset;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateWavyLayer(string name, Vector4 color, float totalDistance,
        float baseTop, float thickness, int numWaves, float amplitude,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile)
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
            float t = i / (float)numPoints;
            
            float wave = amplitude * MathF.Sin(t * MathF.PI * 2f * numWaves);
            float topElev = baseTop + wave;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateTiltedLayer(string name, Vector4 color, float totalDistance,
        float topAtStart, float thickness, float tiltAngle)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        float tiltRad = tiltAngle * MathF.PI / 180f;
        float slope = MathF.Tan(tiltRad);
        
        int numPoints = 50;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float topElev = topAtStart - (x / totalDistance) * totalDistance * slope;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateBasinLayer(string name, Vector4 color, float totalDistance,
        float centerX, float basinWidth, float baseTop, float thickness, float basinDepth)
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
            float basinFactor = Math.Max(0, 1 - (distFromCenter / (basinWidth / 2f)));
            basinFactor = basinFactor * basinFactor;
            
            float subsidence = basinDepth * basinFactor;
            float topElev = baseTop - subsidence;
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateChannelFillFormation(string name, Vector4 color, float totalDistance,
        float channelCenter, float channelWidth, float incisionDepth,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile)
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
            float distFromCenter = Math.Abs(x - channelCenter);
            
            if (distFromCenter < channelWidth / 2f)
            {
                float topoElev = GetTopographyElevationAt(profile, x);
                float channelFactor = 1 - (distFromCenter / (channelWidth / 2f));
                channelFactor = channelFactor * channelFactor;
                float channelBottom = topoElev - incisionDepth * channelFactor;
                
                formation.TopBoundary.Add(new Vector2(x, topoElev));
                formation.BottomBoundary.Add(new Vector2(x, channelBottom));
            }
        }
        
        return formation;
    }
    
    private static void UpdateTopographyWithChannel(GeologicalMapping.ProfileGenerator.TopographicProfile profile,
        float totalDistance, float channelCenter, float channelWidth, float incisionDepth)
    {
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float x = profile.Points[i].Distance;
            float distFromCenter = Math.Abs(x - channelCenter);
            
            if (distFromCenter < channelWidth / 2f)
            {
                float channelFactor = 1 - (distFromCenter / (channelWidth / 2f));
                channelFactor = channelFactor * channelFactor;
                float incision = incisionDepth * channelFactor;
                
                profile.Points[i].Elevation -= incision;
                profile.Points[i].Position = new Vector2(x, profile.Points[i].Elevation);
            }
        }
        
        profile.MinElevation = profile.Points.Min(p => p.Elevation);
        profile.MaxElevation = profile.Points.Max(p => p.Elevation);
    }
    
    private static void OffsetFormationByFault(ProjectedFormation formation, float faultX, float displacement, bool offsetWestSide)
    {
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            if ((offsetWestSide && formation.TopBoundary[i].X < faultX) ||
                (!offsetWestSide && formation.TopBoundary[i].X >= faultX))
            {
                formation.TopBoundary[i] = new Vector2(
                    formation.TopBoundary[i].X,
                    formation.TopBoundary[i].Y - displacement
                );
            }
        }
        
        for (int i = 0; i < formation.BottomBoundary.Count; i++)
        {
            if ((offsetWestSide && formation.BottomBoundary[i].X < faultX) ||
                (!offsetWestSide && formation.BottomBoundary[i].X >= faultX))
            {
                formation.BottomBoundary[i] = new Vector2(
                    formation.BottomBoundary[i].X,
                    formation.BottomBoundary[i].Y - displacement
                );
            }
        }
    }
    
    #endregion
    
    public static string GetPresetName(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Simple Dipping Layers",
        PresetScenario.ErodedAnticline => "Eroded Anticline with Valley Fill",
        PresetScenario.ErodedSyncline => "Eroded Syncline",
        PresetScenario.JuraMountains => "Jura Mountains (Gentle Folds + 4 Thrusts)",
        PresetScenario.FaultedLayers => "Faulted Layers (Graben)",
        PresetScenario.ThrustFault => "Thrust Fault System",
        PresetScenario.FoldedSequence => "Folded Sequence with Reverse Fault",
        PresetScenario.UnconformitySequence => "Angular Unconformity with Fault",
        PresetScenario.BasinFilling => "Sedimentary Basin",
        PresetScenario.ChannelFill => "Incised Valley Channel",
        _ => "Unknown"
    };
    
    public static string GetPresetDescription(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Dipping sedimentary sequence on crystalline basement",
        PresetScenario.ErodedAnticline => "Anticline eroded and filled, with normal fault",
        PresetScenario.ErodedSyncline => "Syncline fold on crystalline basement",
        PresetScenario.JuraMountains => "Realistic Jura-style gentle folds with 4 thrust faults on basement",
        PresetScenario.FaultedLayers => "Graben structure with two normal faults",
        PresetScenario.ThrustFault => "Major thrust system with footwall shortcut",
        PresetScenario.FoldedSequence => "Multiple folds cut by reverse fault, on basement",
        PresetScenario.UnconformitySequence => "Angular unconformity: tilted lower sequence (cut by fault) overlain by flat upper sequence, all on basement",
        PresetScenario.BasinFilling => "Progressive basin filling with basin-bounding fault",
        PresetScenario.ChannelFill => "Incised valley with fluvial deposits",
        _ => "Unknown"
    };
}